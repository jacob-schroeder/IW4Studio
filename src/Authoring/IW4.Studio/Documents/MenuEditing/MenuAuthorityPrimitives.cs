using IW4.Studio.Documents;

namespace IW4.Studio.Documents.MenuEditing;

public enum MenuAuthorityOccurrenceKind
{
    TopLevelDefinition,
    MenuFileInlineDefinition,
    MenuFileRegistration
}

public sealed record MenuAuthorityIssue(
    string NormalizedName,
    TargetZoneRowIdentity RowIdentity,
    int? RegistrationIndex,
    string Message);
