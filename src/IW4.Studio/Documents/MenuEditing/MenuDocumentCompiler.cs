using IW4.Assets.Assets.Menu;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.Studio.Documents;

namespace IW4.Studio.Documents.MenuEditing;

/// <summary>
/// Applies one typed edit to a fresh detached graph. Recursive event and
/// expression nodes are reused from that fresh clone, preserving their
/// topology while editable aggregate nodes are rebuilt by value.
/// </summary>
internal static partial class MenuDocumentCompiler
{
    public static MenuEditResult Apply(
        MenuBuildData source,
        MenuDocumentIdentity identity,
        MenuEdit edit)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(edit);
        return ApplyOwned(source.Copy(), identity.Clone(), edit);
    }

    /// <summary>
    /// Applies an edit to an already detached graph. MenuFile uses this form
    /// so untouched definitions retain sharing with the edited definition.
    /// </summary>
    public static MenuEditResult ApplyOwned(
        MenuBuildData source,
        MenuDocumentIdentity identity,
        MenuEdit edit)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(edit);
        if (!source.IsComplete)
            throw new InvalidOperationException(
                "An unresolved Menu registration cannot be edited as a definition.");
        if (source.Definition.Items.Count != identity.Items.Count)
            throw new InvalidDataException(
                "Menu editor item identities do not match the detached item table.");

        MenuDefAsset definition = source.Definition;
        MenuDocumentIdentity nextIdentity = identity;
        switch (edit)
        {
            case ReplaceMenuSettingsEdit replace:
                ArgumentNullException.ThrowIfNull(replace.Value);
                definition = BuildMenu(definition, settings: replace.Value);
                break;

            case ReplaceRootWindowEdit replace:
                ArgumentNullException.ThrowIfNull(replace.Value);
                RequireLockedRootName(definition.Window.Name, replace.Value.Name);
                definition = BuildMenu(
                    definition,
                    window: BuildWindow(definition.Window, replace.Value));
                break;

            case ReplaceItemEdit replace:
            {
                ArgumentNullException.ThrowIfNull(replace.Value);
                int index = ItemIndex(identity, replace.ItemId);
                ItemDefAsset existing = RequireItem(definition, index);
                definition = ReplaceItem(
                    definition,
                    index,
                    BuildItem(
                        existing,
                        replace.Value,
                        rebuildPayload: false));
                break;
            }

            case ReplaceItemPayloadEdit replace:
            {
                ArgumentNullException.ThrowIfNull(replace.Value);
                int index = ItemIndex(identity, replace.ItemId);
                ItemDefAsset existing = RequireItem(definition, index);
                definition = ReplaceItem(
                    definition,
                    index,
                    BuildItem(
                        existing,
                        replace.Value,
                        rebuildPayload: true));
                break;
            }

            case ReplaceItemWindowEdit replace:
            {
                ArgumentNullException.ThrowIfNull(replace.Value);
                int index = ItemIndex(identity, replace.ItemId);
                ItemDefAsset existing = RequireItem(definition, index);
                MenuItemValue current = SnapshotItem(source, identity, index);
                definition = ReplaceItem(
                    definition,
                    index,
                    BuildItem(
                        existing,
                        current with
                        {
                            Window = replace.Value
                        },
                        rebuildPayload: false));
                break;
            }

            case AddMenuItemEdit add:
            {
                if (!Enum.IsDefined(add.Type))
                    throw new ArgumentOutOfRangeException(nameof(add.Type));
                int index = InsertIndex(add.InsertIndex, definition.Items.Count);
                var item = BuildItem(
                    null,
                    MenuItemDefaults.CreateValue(add.Type, add.Name),
                    rebuildPayload: true);
                var items = definition.Items.ToList();
                items.Insert(index, new ItemDefReference(index, new XPointer<ItemDefAsset>(-1), item));
                definition = BuildMenu(definition, items: Reindex(items));
                var identities = identity.Items.ToList();
                identities.Insert(
                    index,
                    new MenuItemIdentity(MenuNodeId.New(), MenuNodeId.New()));
                nextIdentity = identity.WithItems(identities);
                break;
            }

            case RemoveMenuItemEdit remove:
            {
                int index = ItemIndex(identity, remove.ItemId);
                var items = definition.Items.ToList();
                items.RemoveAt(index);
                definition = BuildMenu(definition, items: Reindex(items));
                var identities = identity.Items.ToList();
                identities.RemoveAt(index);
                nextIdentity = identity.WithItems(identities);
                break;
            }

            case MoveMenuItemEdit move:
            {
                int sourceIndex = ItemIndex(identity, move.ItemId);
                int destinationIndex = ExistingDestination(
                    move.DestinationIndex,
                    definition.Items.Count);
                if (sourceIndex == destinationIndex)
                    return new MenuEditResult(source, identity);
                var items = definition.Items.ToList();
                ItemDefReference item = items[sourceIndex];
                items.RemoveAt(sourceIndex);
                items.Insert(destinationIndex, item);
                definition = BuildMenu(definition, items: Reindex(items));
                var identities = identity.Items.ToList();
                MenuItemIdentity itemIdentity = identities[sourceIndex];
                identities.RemoveAt(sourceIndex);
                identities.Insert(destinationIndex, itemIdentity);
                nextIdentity = identity.WithItems(identities);
                break;
            }

            case DuplicateMenuItemEdit duplicate:
            {
                int sourceIndex = ItemIndex(identity, duplicate.ItemId);
                int insertIndex = InsertIndex(
                    duplicate.InsertIndex ?? sourceIndex + 1,
                    definition.Items.Count);
                ItemDefAsset sourceItem = RequireItem(definition, sourceIndex);
                ItemDefAsset copiedItem = new MenuGraphClone(
                        preserveSourceProvenance: false)
                    .CloneItem(sourceItem)
                    ?? throw new InvalidDataException("A resolved Menu item could not be duplicated.");
                var items = definition.Items.ToList();
                items.Insert(
                    insertIndex,
                    new ItemDefReference(
                        insertIndex,
                        new XPointer<ItemDefAsset>(-1),
                        copiedItem));
                definition = BuildMenu(definition, items: Reindex(items));
                var identities = identity.Items.ToList();
                identities.Insert(
                    insertIndex,
                    new MenuItemIdentity(MenuNodeId.New(), MenuNodeId.New()));
                nextIdentity = identity.WithItems(identities);
                break;
            }

            case ChangeMenuItemTypeEdit change:
            {
                int index = ItemIndex(identity, change.ItemId);
                ItemDefAsset existing = RequireItem(definition, index);
                MenuItemValue current = SnapshotItem(source, identity, index);
                MenuItemValue changed = MenuItemDefaults.ChangeType(current, change.Type);
                definition = ReplaceItem(
                    definition,
                    index,
                    BuildItem(
                        existing,
                        changed,
                        rebuildPayload: true));
                break;
            }

            default:
                throw new InvalidDataException(
                    $"Unsupported Menu edit '{edit.GetType().Name}'.");
        }

        return new MenuEditResult(
            MenuBuildData.CreateOwned(definition, source.IsComplete),
            nextIdentity);
    }
}
