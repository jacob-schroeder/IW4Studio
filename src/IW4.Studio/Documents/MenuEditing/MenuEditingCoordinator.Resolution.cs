using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;

namespace IW4.Studio.Documents.MenuEditing;

public sealed partial class MenuEditingCoordinator
{
    private MenuAuthorityResolutionSnapshot Resolve(
        CapturedMenuAuthorityState state,
        string menuName)
    {
        string lookupName = XAssetStableIdentity.GetLookupSpelling(menuName);
        if (string.IsNullOrWhiteSpace(lookupName))
            throw new ArgumentException("Menu identity cannot be empty.", nameof(menuName));
        string normalizedName = XAssetStableIdentity.NormalizeLookupName(
            lookupName);

        MenuAuthorityIssue[] issues = state.Authorities.Issues
            .Where(issue => string.Equals(
                issue.NormalizedName,
                normalizedName,
                StringComparison.Ordinal))
            .ToArray();
        if (state.Authorities.TryResolve(
                normalizedName,
                out MenuDefinitionAuthority? authority))
        {
            MenuAuthorityOccurrence owner = authority!.Owner;
            MenuEditorSnapshot menu = MenuAuthorityCapture.SnapshotForOwner(
                state,
                owner);
            var ownerSnapshot = new MenuAuthorityOwnerSnapshot(
                owner.RowIdentity,
                owner.RegistrationIndex,
                owner.RegistrationId,
                owner.Kind);
            MenuAuthorityOccurrenceSnapshot[] occurrences = authority.Occurrences
                .Select(occurrence => new MenuAuthorityOccurrenceSnapshot(
                    occurrence.RowIdentity,
                    occurrence.RowIndex,
                    occurrence.RegistrationIndex,
                    occurrence.RegistrationId,
                    occurrence.Kind,
                    occurrence.OriginalName,
                    occurrence.MaterializesDefinition,
                    IsSameOccurrence(occurrence, owner)))
                .ToArray();
            IReadOnlyList<AssetValidationIssue> ownerValidationIssues =
                MenuEditorValidation.Combine(
                    MenuEditorValidation.Validate(menu),
                    OwnerValidator.Validate(
                        owner.Definition
                        ?? throw new InvalidDataException(
                            "A Menu authority owner has no complete build definition.")));
            return new MenuAuthorityResolutionSnapshot(
                state.Revision,
                lookupName,
                normalizedName,
                issues.Length == 0
                    ? MenuAuthorityResolutionKind.Editable
                    : MenuAuthorityResolutionKind.Conflict,
                menu,
                ownerSnapshot,
                occurrences,
                issues,
                ownerValidationIssues);
        }

        MenuEditorSnapshot? readOnly = _capture.CaptureReadOnlyProvider(
            normalizedName);
        return new MenuAuthorityResolutionSnapshot(
            state.Revision,
            lookupName,
            normalizedName,
            readOnly is null
                ? MenuAuthorityResolutionKind.Unavailable
                : MenuAuthorityResolutionKind.ReadOnlyProvider,
            readOnly,
            owner: null,
            occurrences: [],
            issues: [],
            ownerValidationIssues: []);
    }

    private static string TopLevelMenuName(
        CapturedMenuAuthorityState state,
        TargetZoneRowIdentity rowIdentity)
    {
        TargetZoneRowSource row = state.RequireTargetRow(
            rowIdentity,
            XAssetType.Menu);
        return state.Occurrences
            .FirstOrDefault(occurrence =>
                occurrence.RowIdentity == rowIdentity &&
                occurrence.Kind ==
                    MenuAuthorityOccurrenceKind.TopLevelDefinition)
            ?.OriginalName
            ?? row.ExternalReference?.OriginalSerializedName
            ?? row.OriginalSerializedName
            ?? throw new InvalidDataException(
                $"Target Menu row {rowIdentity.SerializedIndex} has no logical identity.");
    }

    private static string MenuFileRegistrationName(
        CapturedMenuAuthorityState state,
        TargetZoneRowIdentity menuFileRowIdentity,
        MenuRegistrationId registrationId)
    {
        CapturedMenuFileRow row = state.RequireMenuFileRow(
            menuFileRowIdentity);
        MenuFileRegistrationSnapshot registration = row.Snapshot.Registrations
            .SingleOrDefault(value => value.Id == registrationId)
            ?? throw new KeyNotFoundException(
                $"MenuFile registration '{registrationId}' is not present in target row {menuFileRowIdentity.SerializedIndex}.");
        return registration.Name
            ?? throw new InvalidDataException(
                $"MenuFile registration '{registrationId}' has no logical Menu identity.");
    }

    private static bool IsSameOccurrence(
        MenuAuthorityOccurrence left,
        MenuAuthorityOccurrence right) =>
        left.RowIdentity == right.RowIdentity &&
        left.RegistrationIndex == right.RegistrationIndex &&
        left.Kind == right.Kind;
}
