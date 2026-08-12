using IW4.Studio.Documents;

namespace IW4.Studio.Documents.MenuEditing;

/// <summary>How one logical Menu can be presented and edited.</summary>
public enum MenuAuthorityResolutionKind
{
    Editable,
    ReadOnlyProvider,
    Unavailable,
    Conflict
}

/// <summary>
/// Scalar description of the target occurrence that owns one logical Menu.
/// It contains no build graph, runtime asset, or editor-local selection.
/// </summary>
public sealed record MenuAuthorityOwnerSnapshot(
    TargetZoneRowIdentity RowIdentity,
    int RegistrationIndex,
    MenuRegistrationId? RegistrationId,
    MenuAuthorityOccurrenceKind Kind);

/// <summary>
/// Scalar description of one target occurrence of a logical Menu. The
/// serialized traversal coordinates make the first-full authority decision
/// visible without exposing mutable build data.
/// </summary>
public sealed record MenuAuthorityOccurrenceSnapshot(
    TargetZoneRowIdentity RowIdentity,
    int RowIndex,
    int RegistrationIndex,
    MenuRegistrationId? RegistrationId,
    MenuAuthorityOccurrenceKind Kind,
    string OriginalName,
    bool MaterializesDefinition,
    bool IsAuthority);

/// <summary>
/// Immutable point-in-time resolution of one logical Menu. An editable
/// resolution always points at the first full target definition in serialized
/// traversal order. A resolved dependency provider is exposed read-only only
/// when no target definition owns the name.
/// </summary>
public sealed class MenuAuthorityResolutionSnapshot
{
    private readonly IReadOnlyList<MenuAuthorityOccurrenceSnapshot> _occurrences;
    private readonly IReadOnlyList<MenuAuthorityIssue> _issues;
    private readonly IReadOnlyList<AssetValidationIssue>
        _ownerValidationIssues;

    internal MenuAuthorityResolutionSnapshot(
        long revision,
        string requestedName,
        string normalizedName,
        MenuAuthorityResolutionKind kind,
        MenuEditorSnapshot? menu,
        MenuAuthorityOwnerSnapshot? owner,
        IEnumerable<MenuAuthorityOccurrenceSnapshot> occurrences,
        IEnumerable<MenuAuthorityIssue> issues,
        IEnumerable<AssetValidationIssue> ownerValidationIssues)
    {
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedName);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName);
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(ownerValidationIssues);
        if (kind == MenuAuthorityResolutionKind.Editable &&
            (menu is null || owner is null))
        {
            throw new InvalidDataException(
                "An editable Menu authority resolution requires a target owner and snapshot.");
        }
        if (kind == MenuAuthorityResolutionKind.ReadOnlyProvider &&
            (menu is null || owner is not null))
        {
            throw new InvalidDataException(
                "A read-only Menu provider resolution requires content without a target owner.");
        }
        if (kind == MenuAuthorityResolutionKind.Unavailable &&
            (menu is not null || owner is not null))
        {
            throw new InvalidDataException(
                "An unavailable Menu resolution cannot expose content or an owner.");
        }
        if (kind == MenuAuthorityResolutionKind.Conflict &&
            (menu is null || owner is null))
        {
            throw new InvalidDataException(
                "A conflicting Menu authority resolution requires its first target owner.");
        }

        Revision = revision;
        RequestedName = requestedName;
        NormalizedName = normalizedName;
        Kind = kind;
        Menu = menu;
        Owner = owner;
        _occurrences = Array.AsReadOnly(occurrences.ToArray());
        _issues = Array.AsReadOnly(issues.ToArray());
        _ownerValidationIssues = Array.AsReadOnly(
            ownerValidationIssues.ToArray());
        if (_ownerValidationIssues.Any(issue => issue is null))
        {
            throw new InvalidDataException(
                "A Menu authority resolution cannot contain a null owner validation issue.");
        }
    }

    public long Revision { get; }

    public string RequestedName { get; }

    public string NormalizedName { get; }

    public MenuAuthorityResolutionKind Kind { get; }

    public bool CanEdit => Kind == MenuAuthorityResolutionKind.Editable;

    public MenuEditorSnapshot? Menu { get; }

    public MenuAuthorityOwnerSnapshot? Owner { get; }

    public IReadOnlyList<MenuAuthorityOccurrenceSnapshot> Occurrences =>
        _occurrences;

    public IReadOnlyList<MenuAuthorityIssue> Issues => _issues;

    /// <summary>
    /// Editor validation for the exact target definition that owns this
    /// resolution. Authority conflicts remain available separately through
    /// <see cref="Issues"/>.
    /// </summary>
    public IReadOnlyList<AssetValidationIssue> OwnerValidationIssues =>
        _ownerValidationIssues;
}

public sealed record MenuAuthorityEditResult(
    bool Changed,
    MenuAuthorityResolutionSnapshot Resolution);

public sealed record MenuFileEditResult(
    bool Changed,
    MenuFileEditorSnapshot MenuFile);

public sealed record MenuFileRevertResult(
    bool Changed,
    MenuFileEditorSnapshot MenuFile);

public enum MenuEditingCoordinatorChangeKind
{
    MenuEdited,
    MenuFileEdited,
    MenuReverted,
    MenuFileReverted,
    EditingSessionChanged
}

/// <summary>One document authoring change observed by the coordinator.</summary>
public sealed class MenuEditingCoordinatorChangedEventArgs : EventArgs
{
    internal MenuEditingCoordinatorChangedEventArgs(
        MenuEditingCoordinatorChangeKind kind,
        long revision,
        TargetZoneRowIdentity? rowIdentity,
        string? normalizedMenuName,
        MenuAuthorityResolutionSnapshot? resolution)
    {
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        Kind = kind;
        Revision = revision;
        RowIdentity = rowIdentity;
        NormalizedMenuName = normalizedMenuName;
        Resolution = resolution;
    }

    public MenuEditingCoordinatorChangeKind Kind { get; }

    public long Revision { get; }

    public TargetZoneRowIdentity? RowIdentity { get; }

    public string? NormalizedMenuName { get; }

    public MenuAuthorityResolutionSnapshot? Resolution { get; }
}
