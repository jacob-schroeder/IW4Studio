using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

/// <summary>Desktop-safe static-dvar support-table row.</summary>
public sealed record BehaviorStaticDvarReference(int Index, string? Name);

/// <summary>Desktop-safe reusable-expression support-table row.</summary>
public sealed record BehaviorReusableExpressionReference(BehaviorReusableExpressionId Id);

/// <summary>Desktop-safe UI-string support-table row.</summary>
public sealed record BehaviorUiStringReference(int Index, string? Value);

/// <summary>
/// Semantic projection of <see cref="ExpressionSupportingData"/>. The raw
/// object remains internal so Desktop can select named support values without
/// seeing pointers, entries, or runtime cache state.
/// </summary>
public sealed class BehaviorExpressionSupport
{
    private readonly IReadOnlyList<BehaviorStaticDvarReference> _staticDvars;
    private readonly IReadOnlyList<BehaviorReusableExpressionReference> _functions;
    private readonly IReadOnlyList<BehaviorUiStringReference> _strings;
    private readonly Dictionary<int, BehaviorStaticDvarReference> _staticDvarsByIndex;
    private readonly Dictionary<string, BehaviorStaticDvarReference> _staticDvarsByName;
    private readonly Dictionary<Statement, BehaviorReusableExpressionId> _functionIds;
    private readonly Dictionary<BehaviorReusableExpressionId, Statement?> _functionStatements;

    private BehaviorExpressionSupport(
        ExpressionSupportingData? source,
        IEnumerable<BehaviorStaticDvarReference> staticDvars,
        IEnumerable<(BehaviorReusableExpressionId Id, Statement? Statement)> functions,
        IEnumerable<BehaviorUiStringReference> strings)
    {
        Source = source;
        var functionRows = functions
            .OrderBy(value => value.Id.Index)
            .ToArray();
        _staticDvars = Array.AsReadOnly(staticDvars.OrderBy(value => value.Index).ToArray());
        _functions = Array.AsReadOnly(functionRows
            .Select(value => new BehaviorReusableExpressionReference(value.Id))
            .ToArray());
        _strings = Array.AsReadOnly(strings.OrderBy(value => value.Index).ToArray());
        _staticDvarsByIndex = _staticDvars.ToDictionary(value => value.Index);
        _staticDvarsByName = _staticDvars
            .Where(value => !string.IsNullOrWhiteSpace(value.Name))
            .GroupBy(value => value.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(value => value.Key, value => value.First(), StringComparer.OrdinalIgnoreCase);
        _functionIds = new Dictionary<Statement, BehaviorReusableExpressionId>(ReferenceEqualityComparer.Instance);
        _functionStatements = [];
        foreach ((BehaviorReusableExpressionId id, Statement? statement) in functionRows)
        {
            _functionStatements.Add(id, statement);
            if (statement is not null)
                _functionIds.TryAdd(statement, id);
        }
    }

    public static BehaviorExpressionSupport Empty { get; } = new(
        null,
        [],
        [],
        []);

    public IReadOnlyList<BehaviorStaticDvarReference> StaticDvars => _staticDvars;
    public IReadOnlyList<BehaviorReusableExpressionReference> ReusableExpressions => _functions;
    public IReadOnlyList<BehaviorUiStringReference> UiStrings => _strings;

    /// <summary>
    /// True when this projection owns an explicit native support table, even
    /// when all three tables are present but empty.
    /// </summary>
    public bool HasSourceTable => Source is not null;

    /// <summary>
    /// Reports whether two semantic projections represent the same native
    /// support-data object in one detached Menu graph.
    /// </summary>
    public bool HasSameSourceTable(BehaviorExpressionSupport? other) =>
        other is not null && Source is not null &&
        ReferenceEquals(Source, other.Source);

    internal ExpressionSupportingData? Source { get; }

    /// <summary>Creates a support projection from one loaded/detached menu graph.</summary>
    public static BehaviorExpressionSupport Import(ExpressionSupportingData? source)
    {
        if (source is null)
            return Empty;

        return new BehaviorExpressionSupport(
            source,
            source.StaticDvarList.LoadedStaticDvars.Select(value =>
                new BehaviorStaticDvarReference(
                    value.Index,
                    value.StaticDvar?.DvarNameString)),
            source.UiFunctions.LoadedFunctions.Select(value =>
                (new BehaviorReusableExpressionId(value.Index), value.Statement)),
            source.UiStrings.LoadedStrings.Select(value =>
                new BehaviorUiStringReference(value.Index, value.Value)));
    }

    public bool TryGetStaticDvar(int index, out BehaviorStaticDvarReference reference) =>
        _staticDvarsByIndex.TryGetValue(index, out reference!);

    public bool TryGetStaticDvar(string name, out BehaviorStaticDvarReference reference)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            reference = null!;
            return false;
        }

        return _staticDvarsByName.TryGetValue(name, out reference!);
    }

    public bool Contains(BehaviorReusableExpressionId id) =>
        _functionStatements.ContainsKey(id);

    /// <summary>
    /// Creates a semantic projection with new static-dvar rows appended after
    /// every imported row. This is a draft-only value operation; the document
    /// compiler remains responsible for materializing native table rows.
    /// </summary>
    public BehaviorExpressionSupport WithAppendedStaticDvars(
        IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        string[] additions = names
            .Select(name => name?.Trim() ?? string.Empty)
            .ToArray();
        if (additions.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "A static-dvar row requires a non-empty name.",
                nameof(names));
        }

        var knownNames = new HashSet<string>(
            _staticDvars
                .Where(value => !string.IsNullOrWhiteSpace(value.Name))
                .Select(value => value.Name!),
            StringComparer.OrdinalIgnoreCase);
        if (additions.Any(name => !knownNames.Add(name!)))
        {
            throw new ArgumentException(
                "A static-dvar row already exists for one of the supplied names.",
                nameof(names));
        }

        var rows = _staticDvars.ToList();
        int nextIndex = rows.Count;
        rows.AddRange(additions.Select(name =>
            new BehaviorStaticDvarReference(nextIndex++, name)));
        return new BehaviorExpressionSupport(
            Source,
            rows,
            _functionStatements.Select(value => (value.Key, value.Value)),
            _strings);
    }

    internal bool TryGetReusableExpression(
        Statement statement,
        out BehaviorReusableExpressionId id) =>
        _functionIds.TryGetValue(statement, out id);

    internal bool TryResolveReusableExpression(
        BehaviorReusableExpressionId id,
        out Statement? statement) =>
        _functionStatements.TryGetValue(id, out statement);

    private sealed class ReferenceEqualityComparer : IEqualityComparer<Statement>
    {
        public static ReferenceEqualityComparer Instance { get; } = new();

        public bool Equals(Statement? x, Statement? y) => ReferenceEquals(x, y);
        public int GetHashCode(Statement obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
