using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;
using IW4.Studio.Documents.MenuEditing.Behavior;
using IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

namespace IW4.Studio.Documents.MenuEditing;

public sealed partial class MenuEditingCoordinator
{
    public string? ValidateNewMenuName(string? menuName)
    {
        ThrowIfDisposed();
        return _session.ValidateNewAssetName(XAssetType.Menu, menuName);
    }

    public MenuAuthorityEditResult ApplyTopLevelMenuEdit(
        TargetZoneRowIdentity rowIdentity,
        MenuAuthorityResolutionSnapshot expectedResolution,
        MenuEdit edit)
    {
        ThrowIfDisposed();
        RequireCurrent(expectedResolution);
        string name = RequireMenu(rowIdentity).Current.Window.Name ??
            throw new InvalidDataException("The top-level Menu has no name.");
        if (Normalize(name) != expectedResolution.NormalizedName)
        {
            throw new InvalidOperationException(
                "The top-level Menu no longer matches the expected authority.");
        }

        RequireExpectedTopLevelAuthority(
            rowIdentity,
            expectedResolution,
            ResolveMenu(name));

        return ApplyResolvedMenuEdit(expectedResolution, edit);
    }

    public MenuAuthorityEditResult ApplyMenuFileRegistrationEdit(
        TargetZoneRowIdentity rowIdentity,
        MenuRegistrationId registrationId,
        MenuAuthorityResolutionSnapshot expectedResolution,
        MenuEdit edit)
    {
        ThrowIfDisposed();
        RequireCurrent(expectedResolution);
        MenuFileRow row = RequireMenuFile(rowIdentity);
        int index = RegistrationIndex(row.Identity, registrationId);
        string name = row.Identity.Registrations[index].Name ??
            row.Current.Menus[index].CanonicalMenu?.Window.Name ??
            throw new InvalidDataException("The MenuFile registration has no name.");
        if (Normalize(name) != expectedResolution.NormalizedName)
        {
            throw new InvalidOperationException(
                "The selected MenuFile registration no longer matches the expected authority.");
        }

        RequireExpectedRegistrationAuthority(
            rowIdentity,
            registrationId,
            expectedResolution,
            ResolveMenu(name));
        return ApplyResolvedMenuEdit(expectedResolution, edit);
    }

    public MenuFileEditResult ApplyMenuFileEdit(
        TargetZoneRowIdentity rowIdentity,
        MenuFileEdit edit)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(edit);
        if (edit is EditMenuFileRegistrationMenuEdit)
        {
            throw new InvalidOperationException(
                "Nested Menu edits must use ApplyMenuFileRegistrationEdit.");
        }
        if (edit is DuplicateMenuFileRegistrationEdit duplicate)
        {
            string? nameError = ValidateNewMenuName(duplicate.NewMenuName);
            if (nameError is not null)
                throw new ArgumentException(nameError, nameof(edit));
        }

        MenuFileRow row = RequireMenuFile(rowIdentity);
        MenuFileAsset candidate = MenuAssetProjector.Apply(
            row.Current,
            row.Identity,
            edit,
            out MenuFileDocumentIdentity candidateIdentity);
        MenuFileEditorSnapshot candidateSnapshot = MenuAssetProjector.Project(
            candidate,
            candidateIdentity);
        EnsureValid(candidateSnapshot);

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
            return new MenuFileEditResult(false, candidateSnapshot);
        row.Current = candidate;
        row.Identity = candidateIdentity;
        AdvanceAuthorityRevision();
        RaiseChanged(
            MenuEditingCoordinatorChangeKind.MenuFileEdited,
            rowIdentity,
            normalizedMenuName: null,
            resolution: null);
        return new MenuFileEditResult(true, candidateSnapshot);
    }

    private MenuAuthorityEditResult ApplyResolvedMenuEdit(
        MenuAuthorityResolutionSnapshot expectedResolution,
        MenuEdit edit)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(edit);
        RequireCurrent(expectedResolution);
        if (!expectedResolution.CanEdit ||
            expectedResolution.Owner is not { } owner)
        {
            throw new InvalidOperationException(
                "This Menu authority is not editable.");
        }

        return owner.Kind switch
        {
            MenuAuthorityOccurrenceKind.TopLevelDefinition =>
                ApplyTopLevelOwnerEdit(owner, expectedResolution, edit),
            MenuAuthorityOccurrenceKind.MenuFileInlineDefinition =>
                ApplyInlineOwnerEdit(owner, expectedResolution, edit),
            _ => throw new InvalidDataException(
                "A reference-only Menu occurrence cannot own an edit.")
        };
    }

    private MenuAuthorityEditResult ApplyTopLevelOwnerEdit(
        MenuAuthorityOwnerSnapshot owner,
        MenuAuthorityResolutionSnapshot expectedResolution,
        MenuEdit edit)
    {
        return ApplyMirroredOwnerEdit(owner, expectedResolution, edit);
    }

    private MenuAuthorityEditResult ApplyInlineOwnerEdit(
        MenuAuthorityOwnerSnapshot owner,
        MenuAuthorityResolutionSnapshot expectedResolution,
        MenuEdit edit)
    {
        return ApplyMirroredOwnerEdit(owner, expectedResolution, edit);
    }

    private MenuAuthorityEditResult ApplyMirroredOwnerEdit(
        MenuAuthorityOwnerSnapshot owner,
        MenuAuthorityResolutionSnapshot expectedResolution,
        MenuEdit edit)
    {
        Occurrence[] definitions = Occurrences(expectedResolution.NormalizedName)
            .Where(occurrence => occurrence.Menu is not null)
            .ToArray();
        Occurrence source = definitions.SingleOrDefault(occurrence =>
            occurrence.RowIdentity == owner.RowIdentity &&
            occurrence.RegistrationIndex == owner.RegistrationIndex &&
            occurrence.Kind == owner.Kind) ?? throw new InvalidDataException(
                "The editable Menu authority has no current owner occurrence.");
        MenuDocumentIdentity sourceIdentity = source.Identity ??
            throw new InvalidDataException(
                "The editable Menu authority has no stable node identities.");
        AssetKey editedMenuKey = AssetKey.FromDefinition(source.Menu!);
        MirroredMenuRowUpdate[] updates = definitions
            .GroupBy(occurrence => occurrence.RowIdentity)
            .OrderBy(group => group.Key.SerializedIndex)
            .Select(group => CreateMirroredUpdate(
                group.Key,
                group.OrderBy(occurrence => occurrence.RegistrationIndex).ToArray(),
                sourceIdentity,
                editedMenuKey,
                edit))
            .ToArray();

        if (!_session.PublishAppliedDefinitions(updates.Select(update =>
                (update.RowIdentity, update.Definition, update.Providers)),
                []))
        {
            return new MenuAuthorityEditResult(false, expectedResolution);
        }

        foreach (MirroredMenuRowUpdate update in updates)
            update.Commit();
        AdvanceAuthorityRevision();
        MenuAuthorityResolutionSnapshot resolution = ResolveMenu(
            expectedResolution.RequestedName);
        if (resolution.Kind == MenuAuthorityResolutionKind.Conflict)
        {
            throw new InvalidDataException(
                "A synchronized Menu edit left equivalent definitions in conflict.");
        }

        RaiseChanged(
            MenuEditingCoordinatorChangeKind.MenuEdited,
            owner.RowIdentity,
            expectedResolution.NormalizedName,
            resolution);
        return new MenuAuthorityEditResult(true, resolution);
    }

    private MirroredMenuRowUpdate CreateMirroredUpdate(
        TargetZoneRowIdentity rowIdentity,
        IReadOnlyList<Occurrence> occurrences,
        MenuDocumentIdentity sourceIdentity,
        AssetKey editedMenuKey,
        MenuEdit edit)
    {
        if (occurrences.Count == 1 &&
            occurrences[0].Kind == MenuAuthorityOccurrenceKind.TopLevelDefinition)
        {
            MenuRow row = RequireMenu(rowIdentity);
            MenuDefAsset candidate = MenuAssetProjector.Apply(
                row.Current,
                row.Identity,
                RebindEdit(edit, sourceIdentity, row.Identity),
                out MenuDocumentIdentity nextIdentity);
            EnsureValid(MenuAssetProjector.Project(candidate, nextIdentity));
            return new MirroredMenuRowUpdate(
                rowIdentity,
                candidate,
                [],
                () =>
                {
                    row.Current = candidate;
                    row.Identity = nextIdentity;
                });
        }

        if (occurrences.Any(occurrence =>
                occurrence.Kind != MenuAuthorityOccurrenceKind.MenuFileInlineDefinition))
        {
            throw new InvalidDataException(
                "Complete Menu occurrences from different owner forms cannot share a target row.");
        }

        MenuFileRow menuFileRow = RequireMenuFile(rowIdentity);
        MenuFileAsset candidateFile = menuFileRow.Current;
        MenuFileDocumentIdentity nextFileIdentity = menuFileRow.Identity;
        foreach (Occurrence occurrence in occurrences)
        {
            MenuRegistrationId registrationId = occurrence.RegistrationId ??
                throw new InvalidDataException(
                    "The inline Menu authority has no registration identity.");
            MenuDocumentIdentity targetIdentity = occurrence.Identity ??
                throw new InvalidDataException(
                    "The inline Menu authority has no stable node identities.");
            candidateFile = MenuAssetProjector.Apply(
                candidateFile,
                nextFileIdentity,
                new EditMenuFileRegistrationMenuEdit(
                    registrationId,
                    RebindEdit(edit, sourceIdentity, targetIdentity)),
                out nextFileIdentity);
            MenuDefAsset inlineMenu = candidateFile.Menus[occurrence.RegistrationIndex]
                .CanonicalMenu ?? throw new InvalidDataException(
                    "The inline Menu edit removed its canonical definition.");
            MenuDocumentIdentity inlineIdentity = nextFileIdentity
                .Registrations[occurrence.RegistrationIndex].MenuIdentity ??
                throw new InvalidDataException(
                    "The inline Menu edit lost stable node identities.");
            EnsureValid(MenuAssetProjector.Project(inlineMenu, inlineIdentity));
        }

        EnsureValid(MenuAssetProjector.Project(candidateFile, nextFileIdentity));
        return new MirroredMenuRowUpdate(
            rowIdentity,
            candidateFile,
            MaterializedMenus(candidateFile, nextFileIdentity)
                .Where(menu => AssetKey.FromDefinition(menu) == editedMenuKey)
                .Cast<IW4.Assets.Assets.BaseAsset>()
                .ToArray(),
            () =>
            {
                menuFileRow.Current = candidateFile;
                menuFileRow.Identity = nextFileIdentity;
            });
    }

    private static IReadOnlyList<MenuDefAsset> MaterializedMenus(
        MenuFileAsset definition,
        MenuFileDocumentIdentity identity)
    {
        if (definition.Menus.Count != identity.Registrations.Count)
        {
            throw new InvalidDataException(
                "MenuFile registrations do not match their detached Menu rows.");
        }

        return identity.Registrations
            .Select((registration, index) => registration.MaterializesDefinition
                ? definition.Menus[index].CanonicalMenu ?? throw new InvalidDataException(
                    "A materialized MenuFile registration has no canonical definition.")
                : null)
            .Where(menu => menu is not null)
            .Cast<MenuDefAsset>()
            .ToArray();
    }

    private IReadOnlyDictionary<AssetKey, MenuDefAsset> CurrentFirstMaterializedMenus(
        TargetZoneRowIdentity candidateRowIdentity,
        MenuFileAsset candidate,
        MenuFileDocumentIdentity candidateIdentity,
        IEnumerable<AssetKey> affectedKeys)
    {
        var affected = new HashSet<AssetKey>(affectedKeys);
        var providers = new Dictionary<AssetKey, MenuDefAsset>();
        foreach (WorkspaceAssetCatalogEntry entry in _session.Document.Rows
                     .OrderBy(entry => entry.TargetRowIdentity?.SerializedIndex))
        {
            if (entry.TargetRowIdentity is not { } rowIdentity)
                continue;

            IEnumerable<MenuDefAsset> menus = rowIdentity == candidateRowIdentity
                ? MaterializedMenus(candidate, candidateIdentity)
                : _menus.TryGetValue(rowIdentity, out MenuRow? menuRow)
                    ? [menuRow.Current]
                    : _menuFiles.TryGetValue(rowIdentity, out MenuFileRow? menuFileRow)
                        ? MaterializedMenus(menuFileRow.Current, menuFileRow.Identity)
                        : [];
            foreach (MenuDefAsset menu in menus)
            {
                AssetKey key = AssetKey.FromDefinition(menu);
                if (affected.Contains(key))
                    _ = providers.TryAdd(key, menu);
            }
        }

        return providers;
    }

    private static void RequireExpectedTopLevelAuthority(
        TargetZoneRowIdentity rowIdentity,
        MenuAuthorityResolutionSnapshot expected,
        MenuAuthorityResolutionSnapshot current)
    {
        if (current.NormalizedName != expected.NormalizedName ||
            !SameOwnerCoordinates(current.Owner, expected.Owner) ||
            current.Owner is not
            {
                RowIdentity: var ownerRow,
                Kind: MenuAuthorityOccurrenceKind.TopLevelDefinition
            } || ownerRow != rowIdentity)
        {
            throw new InvalidOperationException(
                "The selected top-level Menu no longer matches the expected authority.");
        }
    }

    private static void RequireExpectedRegistrationAuthority(
        TargetZoneRowIdentity rowIdentity,
        MenuRegistrationId registrationId,
        MenuAuthorityResolutionSnapshot expected,
        MenuAuthorityResolutionSnapshot current)
    {
        MenuAuthorityOccurrenceSnapshot expectedOccurrence = expected.Occurrences
            .SingleOrDefault(occurrence => occurrence.RowIdentity == rowIdentity &&
                occurrence.RegistrationId == registrationId) ??
            throw new InvalidOperationException(
                "The selected MenuFile registration is not present in the expected authority.");
        MenuAuthorityOccurrenceSnapshot currentOccurrence = current.Occurrences
            .SingleOrDefault(occurrence => occurrence.RowIdentity == rowIdentity &&
                occurrence.RegistrationId == registrationId) ??
            throw new InvalidOperationException(
                "The selected MenuFile registration no longer matches the expected authority.");
        if (current.NormalizedName != expected.NormalizedName ||
            !SameOwnerCoordinates(current.Owner, expected.Owner) ||
            currentOccurrence.RegistrationIndex != expectedOccurrence.RegistrationIndex ||
            currentOccurrence.Kind != expectedOccurrence.Kind ||
            currentOccurrence.MaterializesDefinition !=
                expectedOccurrence.MaterializesDefinition)
        {
            throw new InvalidOperationException(
                "The selected MenuFile registration no longer matches the expected authority.");
        }
    }

    private static bool SameOwnerCoordinates(
        MenuAuthorityOwnerSnapshot? left,
        MenuAuthorityOwnerSnapshot? right) =>
        left is null || right is null
            ? left is null && right is null
            : left.RowIdentity == right.RowIdentity &&
                left.RegistrationIndex == right.RegistrationIndex &&
                left.RegistrationId == right.RegistrationId &&
                left.Kind == right.Kind;

    private static MenuEdit RebindEdit(
        MenuEdit edit,
        MenuDocumentIdentity source,
        MenuDocumentIdentity target)
    {
        MenuNodeId Rebind(MenuNodeId id)
        {
            int index = -1;
            for (int candidateIndex = 0;
                candidateIndex < source.Items.Count;
                candidateIndex++)
            {
                if (source.Items[candidateIndex].Id == id)
                {
                    index = candidateIndex;
                    break;
                }
            }

            if (index < 0 || index >= target.Items.Count)
            {
                throw new InvalidOperationException(
                    "The selected Menu item no longer matches the synchronized authority.");
            }

            return target.Items[index].Id;
        }

        return edit switch
        {
            ReplaceMenuBehaviorEdit value => source.Id == target.Id
                ? value
                : value with
                {
                    Value = DetachMenuBehavior(value.Value)
                },
            ReplaceItemEdit value => value with { ItemId = Rebind(value.ItemId) },
            ReplaceItemPayloadEdit value => value with { ItemId = Rebind(value.ItemId) },
            ReplaceItemWindowEdit value => value with { ItemId = Rebind(value.ItemId) },
            ReplaceItemBehaviorEdit value => value with { ItemId = Rebind(value.ItemId) },
            RemoveMenuItemEdit value => value with { ItemId = Rebind(value.ItemId) },
            MoveMenuItemEdit value => value with { ItemId = Rebind(value.ItemId) },
            DuplicateMenuItemEdit value => value with { ItemId = Rebind(value.ItemId) },
            ChangeMenuItemTypeEdit value => value with { ItemId = Rebind(value.ItemId) },
            _ => edit
        };
    }

    private static MenuDefinitionBehaviorBindings DetachMenuBehavior(
        MenuDefinitionBehaviorBindings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new MenuDefinitionBehaviorBindings(
            DetachEventBinding(value.OnOpen, "onOpen"),
            DetachEventBinding(value.OnCloseRequest, "onCloseRequest"),
            DetachEventBinding(value.OnClose, "onClose"),
            DetachEventBinding(value.OnEscape, "onEscape"),
            DetachKeyHandlers(value.KeyHandlers))
        {
            ExpressionSupportDelta = value.ExpressionSupportDelta
        };
    }

    private static MenuBehaviorEventBinding DetachEventBinding(
        MenuBehaviorEventBinding value,
        string path)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new MenuBehaviorEventBinding(
            DetachEventSet(value.Handlers, path),
            default);
    }

    private static MenuBehaviorEventHandlerSet? DetachEventSet(
        MenuBehaviorEventHandlerSet? value,
        string path)
    {
        if (value is null)
            return null;

        var handlers = new MenuBehaviorEventHandlerEntry[value.Handlers.Length];
        MenuBehaviorEventHandler? previous = null;
        for (int index = 0; index < value.Handlers.Length; index++)
        {
            MenuBehaviorEventHandler? handler = value.Handlers[index].Handler;
            if (handler is null)
            {
                throw new InvalidOperationException(
                    $"Menu behavior '{path}' contains an unresolved handler " +
                    "that cannot be copied to a synchronized definition.");
            }
            if (handler is MenuBehaviorElseEventHandler &&
                previous is not MenuBehaviorConditionalEventHandler)
            {
                throw new InvalidOperationException(
                    $"Menu behavior '{path}' contains an orphan else handler " +
                    "that cannot be copied to a synchronized definition.");
            }

            handlers[index] = MenuBehaviorEventHandlerEntry.Create(
                DetachEventHandler(handler, $"{path}.handlers[{index}]"));
            previous = handler;
        }

        return new MenuBehaviorEventHandlerSet(handlers);
    }

    private static MenuBehaviorEventHandler DetachEventHandler(
        MenuBehaviorEventHandler value,
        string path) => value switch
        {
            MenuBehaviorScriptEventHandler script when script.Script is not null =>
                MenuBehaviorScriptEventHandler.Create(script.Script),
            MenuBehaviorScriptEventHandler => throw new InvalidOperationException(
                $"Menu behavior '{path}' contains a script without text and " +
                "cannot be copied to a synchronized definition."),
            MenuBehaviorConditionalEventHandler conditional
                when conditional.Then is not null =>
                MenuBehaviorConditionalEventHandler.Create(
                    DetachExpression(conditional.Condition, $"{path}.condition"),
                    DetachEventSet(conditional.Then, $"{path}.then")!),
            MenuBehaviorConditionalEventHandler => throw new InvalidOperationException(
                $"Menu behavior '{path}' contains an incomplete conditional " +
                "and cannot be copied to a synchronized definition."),
            MenuBehaviorElseEventHandler otherwise
                when otherwise.Handlers is not null =>
                MenuBehaviorElseEventHandler.Create(
                    DetachEventSet(otherwise.Handlers, $"{path}.handlers")!),
            MenuBehaviorElseEventHandler => throw new InvalidOperationException(
                $"Menu behavior '{path}' contains an incomplete else handler " +
                "and cannot be copied to a synchronized definition."),
            MenuBehaviorSetLocalVariableEventHandler local
                when Enum.IsDefined(local.ValueType) &&
                     !string.IsNullOrWhiteSpace(local.Name) =>
                MenuBehaviorSetLocalVariableEventHandler.Create(
                    local.ValueType,
                    local.Name,
                    DetachExpression(local.Expression, $"{path}.expression")),
            MenuBehaviorSetLocalVariableEventHandler =>
                throw new InvalidOperationException(
                    $"Menu behavior '{path}' contains an incomplete set-local " +
                    "handler and cannot be copied to a synchronized definition."),
            MenuBehaviorOpaqueEventHandler => throw new InvalidOperationException(
                $"Menu behavior '{path}' contains an opaque handler that " +
                "cannot be copied to a synchronized definition."),
            _ => throw new InvalidOperationException(
                $"Menu behavior '{path}' contains an unsupported handler that " +
                "cannot be copied to a synchronized definition.")
        };

    private static MenuBehaviorKeyHandlerBindings DetachKeyHandlers(
        MenuBehaviorKeyHandlerBindings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.HasTruncatedImportedTail)
        {
            throw new InvalidOperationException(
                "Menu key handlers contain an unresolved or cyclic tail and " +
                "cannot be copied to a synchronized definition.");
        }

        return new MenuBehaviorKeyHandlerBindings(
            value.Handlers.Select((handler, index) =>
            {
                if (handler.Action is null)
                {
                    throw new InvalidOperationException(
                        $"Menu key handler {index} has no action and cannot be " +
                        "copied to a synchronized definition.");
                }

                return MenuBehaviorKeyHandlerBinding.Create(
                    handler.Key,
                    DetachEventSet(
                        handler.Action,
                        $"keyHandlers[{index}].action")!);
            }));
    }

    private static BehaviorExpression DetachExpression(
        MenuBehaviorExpressionBinding value,
        string path)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Value is null)
        {
            throw new InvalidOperationException(
                $"Menu behavior expression '{path}' is missing and cannot be " +
                "copied to a synchronized definition.");
        }

        return CloneExpression(value.Value, path);
    }

    private static BehaviorExpression CloneExpression(
        BehaviorExpression value,
        string path) => value switch
        {
            BehaviorIntegerExpression integer =>
                new BehaviorIntegerExpression(integer.Value),
            BehaviorFloatExpression number =>
                new BehaviorFloatExpression(number.Value),
            BehaviorStringExpression text =>
                new BehaviorStringExpression(text.Value),
            BehaviorUnaryExpression unary => new BehaviorUnaryExpression(
                unary.Operation,
                CloneExpression(unary.Operand, path)),
            BehaviorBinaryExpression binary => new BehaviorBinaryExpression(
                binary.Operation,
                CloneExpression(binary.Left, path),
                CloneExpression(binary.Right, path)),
            BehaviorCallExpression call => new BehaviorCallExpression(
                call.Operation,
                call.Arguments.Select(argument => CloneExpression(argument, path))),
            BehaviorReusableExpressionReferenceExpression reusable =>
                new BehaviorReusableExpressionReferenceExpression(
                    reusable.ReferenceId),
            BehaviorStaticDvarExpression dvar =>
                new BehaviorStaticDvarExpression(
                    dvar.Operation,
                    new BehaviorStaticDvarReference(
                        dvar.Dvar.Index,
                        dvar.Dvar.Name)),
            BehaviorOpaqueExpression => throw new InvalidOperationException(
                $"Menu behavior expression '{path}' is opaque and cannot be " +
                "copied to a synchronized definition."),
            _ => throw new InvalidOperationException(
                $"Menu behavior expression '{path}' is unsupported and cannot " +
                "be copied to a synchronized definition.")
        };

    private sealed record MirroredMenuRowUpdate(
        TargetZoneRowIdentity RowIdentity,
        IW4.Assets.Assets.BaseAsset Definition,
        IReadOnlyList<IW4.Assets.Assets.BaseAsset> Providers,
        Action Commit);

    private static void EnsureValid(MenuEditorSnapshot snapshot)
    {
        if (MenuAssetProjector.Validate(snapshot).Any(issue =>
                issue.Severity == AssetValidationSeverity.Error))
        {
            throw new InvalidDataException(
                "The Menu edit produced invalid authoring values.");
        }
    }

    private static void EnsureValid(MenuFileEditorSnapshot snapshot)
    {
        if (MenuAssetProjector.Validate(snapshot).Any(issue =>
                issue.Severity == AssetValidationSeverity.Error))
        {
            throw new InvalidDataException(
                "The MenuFile edit produced invalid authoring values.");
        }
    }
}
