using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;

namespace IW4.Studio.Documents.MenuEditing;

/// <summary>
/// Draft-owned detached Menu graph plus editor-only identities. Only immutable
/// snapshots leave this boundary.
/// </summary>
internal sealed class MenuWorkingDocument
{
    private MenuBuildData _data;
    private MenuDocumentIdentity _identity;

    public MenuWorkingDocument(MenuBuildData data)
        : this(data.Copy(), MenuDocumentIdentity.Create(data))
    {
    }

    private MenuWorkingDocument(
        MenuBuildData data,
        MenuDocumentIdentity identity)
    {
        _data = data;
        _identity = identity;
    }

    public MenuEditorSnapshot Snapshot =>
        MenuSnapshotFactory.Create(_data, _identity);

    public MenuBuildData Export() => _data.Copy();

    public void Apply(MenuEdit edit)
    {
        MenuDocumentCompiler.MenuEditResult result =
            MenuDocumentCompiler.Apply(_data, _identity, edit);
        _data = result.Data;
        _identity = result.Identity;
    }

    public void Replace(MenuBuildData value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _data = value.Copy();
        _identity = MenuDocumentIdentity.Create(_data);
    }

    public MenuWorkingDocument Clone() =>
        new(_data.Copy(), _identity.Clone());
}

/// <summary>
/// Draft-owned ordered MenuFile links. Inline definitions share one detached
/// clone context so their recursive graph identity survives unrelated edits.
/// </summary>
internal sealed class MenuFileWorkingDocument
{
    private MenuFileBuildData _data;
    private MenuFileDocumentIdentity _identity;

    public MenuFileWorkingDocument(MenuFileBuildData data)
        : this(data.Copy(), MenuFileDocumentIdentity.Create(data))
    {
    }

    private MenuFileWorkingDocument(
        MenuFileBuildData data,
        MenuFileDocumentIdentity identity)
    {
        _data = data;
        _identity = identity;
    }

    public MenuFileEditorSnapshot Snapshot =>
        MenuSnapshotFactory.Create(_data, _identity);

    public MenuFileBuildData Export() => _data.Copy();

    public void Apply(MenuFileEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        MenuFileBuildData detached = _data.Copy();
        MenuFileDocumentIdentity identity = _identity.Clone();
        var links = detached.MenuLinks.ToList();
        var identities = identity.Registrations.ToList();
        bool preserveImportedPointerProvenance =
            edit is EditMenuFileRegistrationMenuEdit;

        switch (edit)
        {
            case AddExistingMenuRegistrationEdit addExisting:
            {
                NestedXAssetBuildLink link = ExistingMenuLink(addExisting.MenuName);
                int index = InsertIndex(addExisting.InsertIndex, links.Count);
                links.Insert(index, link);
                identities.Insert(index, Identity(link));
                break;
            }

            case RetargetMenuFileRegistrationEdit retarget:
            {
                int index = RegistrationIndex(identity, retarget.RegistrationId);
                NestedXAssetBuildLink link = ExistingMenuLink(retarget.MenuName);
                links[index] = link;
                identities[index] = new MenuFileRegistrationIdentity(
                    identities[index].Id,
                    null);
                break;
            }

            case AddMenuFileRegistrationEdit add:
            {
                NestedXAssetBuildLink link = CloneAndValidate(add.Link);
                int index = InsertIndex(add.InsertIndex, links.Count);
                links.Insert(index, link);
                identities.Insert(index, Identity(link));
                break;
            }

            case RemoveMenuFileRegistrationEdit remove:
            {
                int index = RegistrationIndex(identity, remove.RegistrationId);
                links.RemoveAt(index);
                identities.RemoveAt(index);
                break;
            }

            case MoveMenuFileRegistrationEdit move:
            {
                int sourceIndex = RegistrationIndex(identity, move.RegistrationId);
                int destinationIndex = ExistingDestination(
                    move.DestinationIndex,
                    links.Count);
                if (sourceIndex == destinationIndex)
                    return;
                NestedXAssetBuildLink link = links[sourceIndex];
                MenuFileRegistrationIdentity registrationIdentity = identities[sourceIndex];
                links.RemoveAt(sourceIndex);
                identities.RemoveAt(sourceIndex);
                links.Insert(destinationIndex, link);
                identities.Insert(destinationIndex, registrationIdentity);
                break;
            }

            case DuplicateMenuFileRegistrationEdit duplicate:
            {
                int sourceIndex = RegistrationIndex(identity, duplicate.RegistrationId);
                int insertIndex = InsertIndex(
                    duplicate.InsertIndex ?? sourceIndex + 1,
                    links.Count);
                if (insertIndex <= sourceIndex)
                {
                    throw new InvalidOperationException(
                        "A duplicate Menu registration must follow the definition or alias it references.");
                }

                NestedXAssetBuildLink source = links[sourceIndex];
                var alias = new NestedXAssetBuildLink(
                    source.Reference,
                    NestedXAssetPointerSourceForm.PackedAlias,
                    IncomingDefinition: null);
                links.Insert(insertIndex, alias);
                identities.Insert(
                    insertIndex,
                    new MenuFileRegistrationIdentity(MenuRegistrationId.New(), null));
                break;
            }

            case ReplaceMenuFileRegistrationEdit replace:
            {
                int index = RegistrationIndex(identity, replace.RegistrationId);
                NestedXAssetBuildLink link = CloneAndValidate(replace.Link);
                links[index] = link;
                identities[index] = new MenuFileRegistrationIdentity(
                    identities[index].Id,
                    link.IncomingDefinition is MenuBuildData menu
                        ? MenuDocumentIdentity.Create(menu)
                        : null);
                break;
            }

            case EditMenuFileRegistrationMenuEdit nested:
            {
                ArgumentNullException.ThrowIfNull(nested.Edit);
                int index = RegistrationIndex(identity, nested.RegistrationId);
                NestedXAssetBuildLink link = links[index];
                if (link.IncomingDefinition is not MenuBuildData menu ||
                    identities[index].MenuIdentity is not { } menuIdentity)
                {
                    throw new InvalidOperationException(
                        "A packed MenuFile registration has no owned definition to edit. Open its authority instead.");
                }

                MenuDocumentCompiler.MenuEditResult result =
                    MenuDocumentCompiler.ApplyOwned(menu, menuIdentity, nested.Edit);
                links[index] = link with { IncomingDefinition = result.Data };
                identities[index] = identities[index] with
                {
                    MenuIdentity = result.Identity
                };
                break;
            }

            default:
                throw new InvalidDataException(
                    $"Unsupported MenuFile edit '{edit.GetType().Name}'.");
        }

        if (!preserveImportedPointerProvenance)
            ClearImportedPointerProvenance(links);

        _data = MenuFileBuildData.CreateOwned(detached.Name, links);
        _identity = identity.WithRegistrations(identities);
    }

    public void Replace(MenuFileBuildData value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _data = value.Copy();
        _identity = MenuFileDocumentIdentity.Create(_data);
    }

    public MenuFileWorkingDocument Clone() =>
        new(_data.Copy(), _identity.Clone());

    private static NestedXAssetBuildLink CloneAndValidate(
        NestedXAssetBuildLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (link.Reference.AssetType != XAssetType.Menu)
        {
            throw new InvalidOperationException(
                "A MenuFile registration must reference a Menu asset.");
        }

        bool ownsDefinition = link.SourceForm is
            NestedXAssetPointerSourceForm.Inline or
            NestedXAssetPointerSourceForm.Insert;
        if (ownsDefinition != (link.IncomingDefinition is not null))
        {
            throw new InvalidOperationException(
                "Inline/insert Menu registrations require a definition and packed registrations cannot own one.");
        }
        if (link.IncomingDefinition is not null and not MenuBuildData)
        {
            throw new InvalidOperationException(
                "A MenuFile registration contains a non-Menu definition.");
        }

        return MenuFileBuildData.CreateOwned(null, [link]).Copy().MenuLinks[0];
    }

    private static NestedXAssetBuildLink ExistingMenuLink(string menuName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(menuName);
        string lookupName = XAssetStableIdentity.GetLookupSpelling(menuName);
        if (lookupName.Length == 0)
            throw new ArgumentException("Menu identity cannot be empty.", nameof(menuName));
        if (lookupName.Contains('\0') || lookupName.Any(character => character > byte.MaxValue))
            throw new ArgumentException(
                "Menu identity must be a Latin-1 string without embedded nulls.",
                nameof(menuName));
        return new NestedXAssetBuildLink(
            new SymbolicXAssetReference(XAssetType.Menu, $",{lookupName}"),
            NestedXAssetPointerSourceForm.PackedAlias,
            IncomingDefinition: null);
    }

    private static MenuFileRegistrationIdentity Identity(
        NestedXAssetBuildLink link) =>
        new(
            MenuRegistrationId.New(),
            link.IncomingDefinition is MenuBuildData menu
                ? MenuDocumentIdentity.Create(menu)
                : null);

    private static void ClearImportedPointerProvenance(
        IList<NestedXAssetBuildLink> links)
    {
        for (int index = 0; index < links.Count; index++)
        {
            links[index] = links[index] with
            {
                ImportedPackedRaw = null,
                ImportedOwnerCellRaw = null
            };
        }
    }

    private static int RegistrationIndex(
        MenuFileDocumentIdentity identity,
        MenuRegistrationId id)
    {
        for (int index = 0; index < identity.Registrations.Count; index++)
        {
            if (identity.Registrations[index].Id == id)
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

    private static int ExistingDestination(int requested, int count)
    {
        if (requested < 0 || requested >= count)
            throw new ArgumentOutOfRangeException(nameof(requested));
        return requested;
    }
}
