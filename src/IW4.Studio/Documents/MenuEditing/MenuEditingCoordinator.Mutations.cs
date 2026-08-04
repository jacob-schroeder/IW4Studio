using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;

namespace IW4.Studio.Documents.MenuEditing;

public sealed partial class MenuEditingCoordinator
{
    private MenuAuthorityEditResult ApplyMenuEdit(
        CapturedMenuAuthorityState state,
        MenuAuthorityResolutionSnapshot expectedResolution,
        MenuAuthorityResolutionSnapshot currentResolution,
        MenuEdit edit)
    {
        MenuAuthorityOwnerSnapshot owner = currentResolution.Owner
            ?? throw new InvalidDataException(
                "An editable Menu authority has no target owner.");
        int? targetItemIndex = MenuItemIndex(
            expectedResolution.Menu!,
            edit);
        AuthoredDraftMutation[] mutations = CreateMirroredMutations(
            currentResolution,
            edit,
            targetItemIndex);
        bool changed = _editingSession.MutateAuthoredDraftsAtRevision(
            state.Revision,
            mutations);
        MenuAuthorityResolutionSnapshot updated = Resolve(
            CaptureAuthorityState(),
            expectedResolution.RequestedName);
        if (updated.Kind == MenuAuthorityResolutionKind.Conflict)
        {
            throw new InvalidDataException(
                "An atomic Menu edit left equivalent target definitions in conflict.");
        }
        if (changed)
        {
            RaiseChanged(
                MenuEditingCoordinatorChangeKind.MenuEdited,
                owner.RowIdentity,
                expectedResolution.NormalizedName,
                updated);
        }

        return new MenuAuthorityEditResult(changed, updated);
    }

    private MenuAuthorityResolutionSnapshot RequireCurrentEditableResolution(
        CapturedMenuAuthorityState state,
        MenuAuthorityResolutionSnapshot expectedResolution,
        string currentMenuName)
    {
        MenuAuthorityResolutionSnapshot current = RequireCurrentResolution(
            state,
            expectedResolution,
            currentMenuName);
        if (!expectedResolution.CanEdit)
            ThrowCannotEdit(expectedResolution);
        if (!current.CanEdit)
            ThrowCannotEdit(current);
        return current;
    }

    private MenuAuthorityResolutionSnapshot RequireCurrentResolution(
        CapturedMenuAuthorityState state,
        MenuAuthorityResolutionSnapshot expectedResolution,
        string currentMenuName)
    {
        RequireExpectedRevision(state, expectedResolution);
        string currentNormalizedName = NormalizeMenuName(currentMenuName);
        if (!string.Equals(
                currentNormalizedName,
                expectedResolution.NormalizedName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The selected Menu now resolves to '{currentMenuName}', not " +
                $"the expected '{expectedResolution.RequestedName}'. Refresh " +
                "the editor before applying the staged operation.");
        }

        MenuAuthorityResolutionSnapshot current = Resolve(
            state,
            currentMenuName);
        if (current.Kind != expectedResolution.Kind ||
            !SameOwnerCoordinates(current.Owner, expectedResolution.Owner))
        {
            throw new InvalidOperationException(
                "The Menu authority owner changed after the editor resolution was captured. Refresh the editor before applying the staged operation.");
        }

        return current;
    }

    private void RequireExpectedRevision(
        CapturedMenuAuthorityState state,
        MenuAuthorityResolutionSnapshot expectedResolution)
    {
        if (state.Revision != expectedResolution.Revision)
        {
            throw new InvalidOperationException(
                $"The Menu editor resolved revision " +
                $"{expectedResolution.Revision}, but the document is at " +
                $"revision {state.Revision}. Refresh the editor before " +
                "applying the staged operation.");
        }
        if (expectedResolution.Owner is { } owner &&
            owner.RowIdentity.DocumentId != DocumentId)
        {
            throw new InvalidOperationException(
                "The expected Menu authority belongs to another document.");
        }
        if (expectedResolution.Occurrences.Any(occurrence =>
                occurrence.RowIdentity.DocumentId != DocumentId))
        {
            throw new InvalidDataException(
                "The expected Menu authority contains an occurrence from another document.");
        }
    }

    private static string RequireCurrentRegistrationName(
        CapturedMenuAuthorityState state,
        TargetZoneRowIdentity menuFileRowIdentity,
        MenuRegistrationId registrationId,
        MenuAuthorityResolutionSnapshot expectedResolution)
    {
        MenuAuthorityOccurrenceSnapshot[] matches = expectedResolution.Occurrences
            .Where(occurrence =>
                occurrence.RowIdentity == menuFileRowIdentity &&
                occurrence.RegistrationId == registrationId)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"MenuFile registration '{registrationId}' is not the " +
                "selected occurrence in the expected Menu authority resolution.");
        }

        MenuAuthorityOccurrenceSnapshot expectedOccurrence = matches[0];
        if (!string.Equals(
                NormalizeMenuName(expectedOccurrence.OriginalName),
                expectedResolution.NormalizedName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The expected MenuFile registration does not match its resolved logical Menu name.");
        }

        MenuFileEditorSnapshot current = state
            .RequireMenuFileRow(menuFileRowIdentity)
            .Snapshot;
        int index = expectedOccurrence.RegistrationIndex;
        if (index < 0 || index >= current.Registrations.Count)
        {
            throw new InvalidOperationException(
                "The selected MenuFile registration no longer exists at its resolved position.");
        }

        string currentName = current.Registrations[index].Name
            ?? throw new InvalidDataException(
                "The selected MenuFile registration has no logical Menu identity.");
        if (!string.Equals(
                NormalizeMenuName(currentName),
                expectedResolution.NormalizedName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The selected MenuFile registration was retargeted after the authority resolution was captured.");
        }

        return currentName;
    }

    private AuthoredDraftMutation[] CreateMirroredMutations(
        MenuAuthorityResolutionSnapshot currentResolution,
        MenuEdit edit,
        int? targetItemIndex)
    {
        MenuAuthorityOccurrenceSnapshot[] definitions = currentResolution
            .Occurrences
            .Where(occurrence => occurrence.MaterializesDefinition)
            .OrderBy(occurrence => occurrence.RowIndex)
            .ThenBy(occurrence => occurrence.RegistrationIndex)
            .ToArray();
        if (definitions.Length == 0)
        {
            throw new InvalidDataException(
                "An editable Menu authority contains no complete target definition.");
        }

        IAssetAuthoringAdapter menuAdapter = _adapters.RequireAdapter(
            XAssetType.Menu);
        IAssetAuthoringAdapter menuFileAdapter = _adapters.RequireAdapter(
            XAssetType.MenuFile);
        return definitions
            .GroupBy(occurrence => occurrence.RowIdentity)
            .Select(group => CreateMirroredRowMutation(
                group.Key,
                group.ToArray(),
                currentResolution.NormalizedName,
                edit,
                targetItemIndex,
                menuAdapter,
                menuFileAdapter))
            .ToArray();
    }

    private static AuthoredDraftMutation CreateMirroredRowMutation(
        TargetZoneRowIdentity rowIdentity,
        IReadOnlyList<MenuAuthorityOccurrenceSnapshot> occurrences,
        string expectedNormalizedName,
        MenuEdit edit,
        int? targetItemIndex,
        IAssetAuthoringAdapter menuAdapter,
        IAssetAuthoringAdapter menuFileAdapter)
    {
        if (occurrences.Count == 1 &&
            occurrences[0].Kind ==
                MenuAuthorityOccurrenceKind.TopLevelDefinition)
        {
            return new AuthoredDraftMutation(
                rowIdentity,
                menuAdapter,
                draft => ApplyTopLevelOccurrenceEdit(
                    (MenuDraft)draft,
                    expectedNormalizedName,
                    edit,
                    targetItemIndex));
        }
        if (occurrences.Any(occurrence =>
                occurrence.Kind !=
                    MenuAuthorityOccurrenceKind.MenuFileInlineDefinition))
        {
            throw new InvalidDataException(
                "Complete Menu occurrences from different owner forms cannot share one target row.");
        }

        int[] registrationIndices = occurrences
            .Select(occurrence => occurrence.RegistrationIndex)
            .OrderBy(index => index)
            .ToArray();
        return new AuthoredDraftMutation(
            rowIdentity,
            menuFileAdapter,
            draft =>
            {
                var menuFile = (MenuFileDraft)draft;
                foreach (int registrationIndex in registrationIndices)
                {
                    ApplyInlineMenuEdit(
                        menuFile,
                        registrationIndex,
                        expectedNormalizedName,
                        edit,
                        targetItemIndex);
                }
            });
    }

    private static void ApplyTopLevelOccurrenceEdit(
        MenuDraft draft,
        string expectedNormalizedName,
        MenuEdit edit,
        int? targetItemIndex)
    {
        MenuEditorSnapshot snapshot = draft.Snapshot;
        if (!string.Equals(
                NormalizeMenuName(snapshot.Name ?? string.Empty),
                expectedNormalizedName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The top-level Menu definition no longer matches the resolved logical name.");
        }

        draft.Apply(RebindMenuEdit(snapshot, edit, targetItemIndex));
    }

    private static bool SameOwnerCoordinates(
        MenuAuthorityOwnerSnapshot? left,
        MenuAuthorityOwnerSnapshot? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return left.RowIdentity == right.RowIdentity &&
            left.RegistrationIndex == right.RegistrationIndex &&
            left.Kind == right.Kind;
    }

    private static string NormalizeMenuName(string menuName)
    {
        string lookupName = XAssetStableIdentity.GetLookupSpelling(menuName);
        if (string.IsNullOrWhiteSpace(lookupName))
        {
            throw new InvalidDataException(
                "A resolved Menu occurrence has no logical identity.");
        }

        return XAssetStableIdentity.NormalizeLookupName(lookupName);
    }

    private static void ThrowCannotEdit(
        MenuAuthorityResolutionSnapshot resolution)
    {
        string reason = resolution.Kind switch
        {
            MenuAuthorityResolutionKind.Conflict =>
                "its target definitions conflict",
            MenuAuthorityResolutionKind.ReadOnlyProvider =>
                "it is supplied by a read-only dependency provider",
            _ => "no target definition owns it"
        };
        throw new InvalidOperationException(
            $"Menu '{resolution.RequestedName}' cannot be edited because {reason}.");
    }
}
