using IW4.Assets.Assets.Menu;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.Studio.Documents;
using IW4.Studio.Documents.MenuEditing.Behavior;

namespace IW4.Studio.Documents.MenuEditing;

/// <summary>
/// Applies one typed edit to a fresh detached graph. Recursive event and
/// expression nodes are reused from that fresh clone, preserving their
/// topology while editable aggregate nodes are rebuilt by value.
/// </summary>
internal static partial class MenuDocumentCompiler
{
    public static MenuEditResult Apply(
        MenuDefAsset source,
        MenuDocumentIdentity identity,
        MenuEdit edit)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(edit);
        return ApplyOwned(new MenuGraphClone().CloneMenu(source), identity.Clone(), edit);
    }

    /// <summary>
    /// Applies an edit to an already detached graph. MenuFile uses this form
    /// so untouched definitions retain sharing with the edited definition.
    /// </summary>
    public static MenuEditResult ApplyOwned(
        MenuDefAsset source,
        MenuDocumentIdentity identity,
        MenuEdit edit)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(edit);
        if (source.Items.Count != identity.Items.Count)
            throw new InvalidDataException(
                "Menu editor item identities do not match the detached item table.");

        MenuDefAsset definition = source;
        MenuDocumentIdentity nextIdentity = identity;
        switch (edit)
        {
            case ReplaceMenuSettingsEdit replace:
            {
                ArgumentNullException.ThrowIfNull(replace.Value);
                IReadOnlyList<ItemDefReference>? items =
                    replace.Value.ImageTrack == definition.ImageTrack
                        ? null
                        : BuildItemsForImageTrack(
                            source,
                            identity,
                            replace.Value.ImageTrack);
                definition = BuildMenu(
                    definition,
                    settings: replace.Value,
                    items: items);
                break;
            }

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
                        rebuildPayload: false,
                        imageTrack: definition.ImageTrack));
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
                        rebuildPayload: true,
                        imageTrack: definition.ImageTrack));
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
                        rebuildPayload: false,
                        imageTrack: definition.ImageTrack));
                break;
            }

            case ReplaceItemBehaviorEdit replace:
            {
                ArgumentNullException.ThrowIfNull(replace.Value);
                int index = ItemIndex(identity, replace.ItemId);
                ItemDefAsset existing = RequireItem(definition, index);
                if (existing.Type != ItemDefType.ListBox &&
                    replace.Value.ListBoxDoubleClick.Handlers is not null)
                {
                    throw new InvalidDataException(
                        "List-box double-click behavior can be authored only " +
                        "for a ListBox item.");
                }
                ApplyExpressionSupportDelta(
                    definition.ExpressionDataValue,
                    replace.Value.ExpressionSupportDelta);
                var expressionCodec = new MenuBehaviorExpressionCodec(
                    definition.ExpressionDataValue);
                var behaviorCodec = new MenuItemBehaviorCodec(expressionCodec);
                var validator = new MenuItemBehaviorValidator(expressionCodec);
                MenuItemBehaviorBindings currentBaseline =
                    behaviorCodec.Import(existing);
                expressionCodec.UseCurrentBaseline(currentBaseline);
                MenuBehaviorValidationIssue[] errors = validator
                    .Validate(replace.Value, MenuBehaviorValidationMode.Authored)
                    .Where(issue =>
                        issue.Severity == MenuBehaviorValidationSeverity.Error)
                    .ToArray();
                if (errors.Length != 0)
                {
                    throw new InvalidDataException(string.Join(
                        Environment.NewLine,
                        errors.Select(error => $"{error.Path}: {error.Message}")));
                }

                MenuItemBehaviorAssetBindings behavior =
                    behaviorCodec.Export(replace.Value);
                MenuItemValue current = SnapshotItem(source, identity, index);
                definition = ReplaceItem(
                    definition,
                    index,
                    BuildItem(
                        existing,
                        current,
                        rebuildPayload: false,
                        imageTrack: definition.ImageTrack,
                        behavior: behavior));
                break;
            }

            case AddMenuItemEdit add:
            {
                if (!Enum.IsDefined(add.Type))
                    throw new ArgumentOutOfRangeException(nameof(add.Type));
                int index = InsertIndex(add.InsertIndex, definition.Items.Count);
                var item = BuildItem(
                    null,
                    MenuItemDefaults.CreateValue(
                        add.Type,
                        definition.ImageTrack,
                        add.Name),
                    rebuildPayload: true,
                    imageTrack: definition.ImageTrack);
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
                if (!float.IsFinite(duplicate.OffsetX) ||
                    !float.IsFinite(duplicate.OffsetY))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(duplicate),
                        "Duplicate offsets must be finite.");
                }

                int sourceIndex = ItemIndex(identity, duplicate.ItemId);
                int insertIndex = InsertIndex(
                    duplicate.InsertIndex ?? sourceIndex + 1,
                    definition.Items.Count);
                ItemDefAsset sourceItem = RequireItem(definition, sourceIndex);
                ItemDefAsset copiedItem = new MenuGraphClone(
                        preserveSourceProvenance: false)
                    .CloneItem(sourceItem, definition.ImageTrack)
                    ?? throw new InvalidDataException("A resolved Menu item could not be duplicated.");
                MenuItemValue copiedValue = SnapshotItem(
                    source,
                    identity,
                    sourceIndex);
                MenuRectangleValue copiedRectangle =
                    copiedValue.Window.RectClient;
                float copiedX = copiedRectangle.X + duplicate.OffsetX;
                float copiedY = copiedRectangle.Y + duplicate.OffsetY;
                if (!float.IsFinite(copiedX) || !float.IsFinite(copiedY))
                {
                    throw new OverflowException(
                        "Duplicate offsets produced non-finite Item geometry.");
                }

                copiedItem = BuildItem(
                    copiedItem,
                    copiedValue with
                    {
                        Window = copiedValue.Window with
                        {
                            RectClient = copiedRectangle with
                            {
                                X = copiedX,
                                Y = copiedY
                            }
                        }
                    },
                    rebuildPayload: false,
                    imageTrack: definition.ImageTrack);
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
                        rebuildPayload: true,
                        imageTrack: definition.ImageTrack));
                break;
            }

            default:
                throw new InvalidDataException(
                    $"Unsupported Menu edit '{edit.GetType().Name}'.");
        }

        return new MenuEditResult(
            definition,
            nextIdentity);
    }

    /// <summary>
    /// Materializes the one support-table mutation currently owned by the
    /// ItemDef behavior builder. This runs only on the compiler's detached
    /// graph clone: Desktop carries names and an expected row count, never
    /// native pointers, table cells, or runtime dvar handles.
    /// </summary>
    private static void ApplyExpressionSupportDelta(
        ExpressionSupportingData? support,
        MenuBehaviorExpressionSupportDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        if (delta.IsEmpty)
            return;
        if (support is null)
        {
            throw new InvalidDataException(
                "A Menu without expression support data cannot append static dvars.");
        }

        StaticDvarList current = support.StaticDvarList;
        IReadOnlyList<StaticDvarReference> existing =
            current.LoadedStaticDvars;
        if (current.NumStaticDvars != existing.Count ||
            existing.Count != delta.ExpectedStaticDvarCount)
        {
            throw new InvalidDataException(
                "The Menu static-dvar support table changed while the behavior " +
                "editor was open. Reopen the editor before applying this change.");
        }
        if (existing.Select(row => row.Index)
            .Where((index, position) => index != position)
            .Any())
        {
            throw new InvalidDataException(
                "The Menu static-dvar support table has non-sequential row indexes.");
        }

        var names = new HashSet<string>(
            existing
                .Select(row => row.StaticDvar?.DvarNameString)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!),
            StringComparer.OrdinalIgnoreCase);
        var rows = existing.ToList();
        foreach (string name in delta.StaticDvarNames)
        {
            if (!names.Add(name))
            {
                throw new InvalidDataException(
                    $"The static-dvar support table already contains '{name}'.");
            }

            rows.Add(new StaticDvarReference(
                rows.Count,
                new XPointer<StaticDvar>(-1),
                new StaticDvar
                {
                    Dvar = default,
                    DvarName = new XPointer<string>(-1),
                    DvarNameString = name
                }));
        }

        support.StaticDvarList = new StaticDvarList
        {
            NumStaticDvars = rows.Count,
            StaticDvars = new XPointer<XPointer<StaticDvar>[]>(-1),
            LoadedStaticDvars = rows.ToArray()
        };
    }
}
