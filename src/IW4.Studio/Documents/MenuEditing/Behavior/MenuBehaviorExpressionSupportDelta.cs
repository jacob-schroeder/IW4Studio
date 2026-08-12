using System.Collections.Immutable;

namespace IW4.Studio.Documents.MenuEditing.Behavior;

/// <summary>
/// Immutable, document-owned changes to the Menu expression support graph.
/// The ItemDef builder currently owns only appending named static-dvar rows.
/// It intentionally cannot create a reusable UI-function row: defining one
/// needs a separate expression-definition authority, not an empty Statement.
/// </summary>
public sealed class MenuBehaviorExpressionSupportDelta
{
    private readonly ImmutableArray<string> _staticDvarNames;

    public static MenuBehaviorExpressionSupportDelta Empty { get; } = new(
        expectedStaticDvarCount: 0,
        []);

    public MenuBehaviorExpressionSupportDelta(
        int expectedStaticDvarCount,
        IEnumerable<string> staticDvarNames)
    {
        if (expectedStaticDvarCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedStaticDvarCount));
        }
        ArgumentNullException.ThrowIfNull(staticDvarNames);

        ExpectedStaticDvarCount = expectedStaticDvarCount;
        _staticDvarNames = staticDvarNames
            .Select(name => name?.Trim() ?? string.Empty)
            .ToImmutableArray();
        if (_staticDvarNames.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "A static-dvar row requires a non-empty name.",
                nameof(staticDvarNames));
        }
        if (_staticDvarNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            _staticDvarNames.Length)
        {
            throw new ArgumentException(
                "A static-dvar name may be appended only once per edit.",
                nameof(staticDvarNames));
        }
    }

    /// <summary>
    /// Number of rows visible to the modal before it began appending. The
    /// compiler checks this optimistic concurrency guard before assigning
    /// deterministic new table indexes.
    /// </summary>
    public int ExpectedStaticDvarCount { get; }

    public ImmutableArray<string> StaticDvarNames => _staticDvarNames;

    public bool IsEmpty => _staticDvarNames.IsEmpty;
}
