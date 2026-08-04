using IW4.FastFiles.Emitters.Assets;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;

namespace IW4.Studio.Documents.MenuEditing;

public enum MenuAuthorityOccurrenceKind
{
    TopLevelDefinition,
    MenuFileInlineDefinition,
    MenuFileRegistration
}

/// <summary>
/// One logical Menu occurrence in serialized traversal order. Build-data
/// references are short-lived inputs owned by the caller; the index itself is
/// rebuilt from an immutable editor/save capture whenever authority matters.
/// </summary>
public sealed record MenuAuthorityOccurrence(
    TargetZoneRowIdentity RowIdentity,
    int RowIndex,
    int RegistrationIndex,
    MenuRegistrationId? RegistrationId,
    MenuAuthorityOccurrenceKind Kind,
    string OriginalName,
    MenuBuildData? Definition,
    NestedXAssetPointerSourceForm? SourceForm)
{
    public string NormalizedName =>
        XAssetStableIdentity.NormalizeLookupName(OriginalName);

    public bool MaterializesDefinition =>
        Definition is { IsComplete: true };
}

public sealed record MenuAuthorityIssue(
    string NormalizedName,
    TargetZoneRowIdentity RowIdentity,
    int? RegistrationIndex,
    string Message);

public sealed class MenuDefinitionAuthority
{
    internal MenuDefinitionAuthority(
        MenuAuthorityOccurrence owner,
        IEnumerable<MenuAuthorityOccurrence> occurrences)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Occurrences = Array.AsReadOnly(
            occurrences.OrderBy(MenuAuthorityIndex.TraversalKey).ToArray());
    }

    public string Name => Owner.OriginalName;

    public string NormalizedName => Owner.NormalizedName;

    public MenuAuthorityOccurrence Owner { get; }

    public IReadOnlyList<MenuAuthorityOccurrence> Occurrences { get; }
}

/// <summary>
/// Resolves one editable definition authority per canonical Menu name and
/// reports every later full body whose authored semantics disagree with that
/// authority. Reference-only names intentionally have no authority.
/// </summary>
public sealed class MenuAuthorityIndex
{
    private readonly IReadOnlyDictionary<string, MenuDefinitionAuthority>
        _authorities;

    private MenuAuthorityIndex(
        IReadOnlyDictionary<string, MenuDefinitionAuthority> authorities,
        IEnumerable<MenuAuthorityIssue> issues)
    {
        _authorities = authorities;
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    public IReadOnlyCollection<MenuDefinitionAuthority> Authorities =>
        Array.AsReadOnly(_authorities.Values
            .OrderBy(authority => TraversalKey(authority.Owner))
            .ToArray());

    public IReadOnlyList<MenuAuthorityIssue> Issues { get; }

    public bool HasConflicts => Issues.Count != 0;

    public static MenuAuthorityIndex Build(
        IEnumerable<MenuAuthorityOccurrence> occurrences)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        MenuAuthorityOccurrence[] ordered = occurrences
            .Select(ValidateOccurrence)
            .OrderBy(TraversalKey)
            .ToArray();
        var authorities = new Dictionary<string, MenuDefinitionAuthority>(
            StringComparer.Ordinal);
        var issues = new List<MenuAuthorityIssue>();

        foreach (IGrouping<string, MenuAuthorityOccurrence> group in ordered
                     .GroupBy(value => value.NormalizedName, StringComparer.Ordinal))
        {
            MenuAuthorityOccurrence[] groupOccurrences = group.ToArray();
            MenuAuthorityOccurrence? owner = groupOccurrences
                .FirstOrDefault(value => value.MaterializesDefinition);
            if (owner is null)
                continue;

            string authorityProjection = MenuSemanticProjection.Serialize(
                owner.Definition!.Definition);
            foreach (MenuAuthorityOccurrence candidate in groupOccurrences.Where(
                         value => value.MaterializesDefinition &&
                                  !ReferenceEquals(value, owner)))
            {
                string candidateProjection = MenuSemanticProjection.Serialize(
                    candidate.Definition!.Definition);
                if (string.Equals(
                        authorityProjection,
                        candidateProjection,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                issues.Add(new MenuAuthorityIssue(
                    group.Key,
                    candidate.RowIdentity,
                    candidate.RegistrationIndex >= 0
                        ? candidate.RegistrationIndex
                        : null,
                    $"Menu '{owner.OriginalName}' has a later complete definition that differs from its first serialized authority."));
            }

            authorities.Add(
                group.Key,
                new MenuDefinitionAuthority(owner, groupOccurrences));
        }

        return new MenuAuthorityIndex(authorities, issues);
    }

    public bool TryResolve(
        string name,
        out MenuDefinitionAuthority? authority)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _authorities.TryGetValue(
            XAssetStableIdentity.NormalizeLookupName(name),
            out authority);
    }

    public MenuDefinitionAuthority Resolve(string name) =>
        TryResolve(name, out MenuDefinitionAuthority? authority)
            ? authority!
            : throw new KeyNotFoundException(
                $"No editable Menu definition authority exists for '{name}'.");

    internal static (int Row, int Registration) TraversalKey(
        MenuAuthorityOccurrence occurrence) =>
        (occurrence.RowIndex, occurrence.RegistrationIndex);

    private static MenuAuthorityOccurrence ValidateOccurrence(
        MenuAuthorityOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        if (occurrence.RowIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(occurrence));
        if (string.IsNullOrWhiteSpace(occurrence.OriginalName))
        {
            throw new InvalidDataException(
                "A Menu authority occurrence requires a non-empty logical name.");
        }
        if (occurrence.Kind == MenuAuthorityOccurrenceKind.TopLevelDefinition &&
            occurrence.RegistrationIndex != -1)
        {
            throw new InvalidDataException(
                "A top-level Menu authority occurrence must use registration index -1.");
        }
        if (occurrence.Kind != MenuAuthorityOccurrenceKind.TopLevelDefinition &&
            occurrence.RegistrationIndex < 0)
        {
            throw new InvalidDataException(
                "A MenuFile authority occurrence requires a non-negative registration index.");
        }
        if (occurrence.MaterializesDefinition &&
            occurrence.Kind == MenuAuthorityOccurrenceKind.MenuFileRegistration)
        {
            throw new InvalidDataException(
                "A reference-only MenuFile occurrence cannot materialize a definition.");
        }

        return occurrence;
    }
}
