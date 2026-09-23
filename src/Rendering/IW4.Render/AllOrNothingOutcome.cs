using System.Collections;
using System.Collections.Immutable;

namespace IW4.Render;

/// <summary>
/// Immutable storage for a reference-valued success or an ordered, non-empty
/// failure snapshot. Public result wrappers retain their domain vocabulary;
/// this type owns only their common all-or-nothing invariant.
/// </summary>
internal sealed class AllOrNothingOutcome<TValue, TFailure> :
    IReadOnlyList<TFailure>
    where TValue : class
{
    private readonly ImmutableArray<TFailure> _failures;

    public AllOrNothingOutcome(
        TValue? value,
        IReadOnlyList<TFailure> failures,
        string invalidMessage,
        bool rejectNullFailures = false,
        string? invalidParameterName = "failures",
        string nullParameterName = "failures")
    {
        ArgumentNullException.ThrowIfNull(failures, nullParameterName);
        _failures = ImmutableArray.CreateRange(failures);
        if ((value is null) == _failures.IsEmpty ||
            (rejectNullFailures &&
             _failures.Any(static failure => failure is null)))
        {
            throw invalidParameterName is null
                ? new ArgumentException(invalidMessage)
                : new ArgumentException(invalidMessage, invalidParameterName);
        }

        Value = value;
    }

    public TValue? Value { get; }

    public int Count => _failures.Length;

    public TFailure this[int index] => _failures[index];

    public IEnumerator<TFailure> GetEnumerator() =>
        ((IEnumerable<TFailure>)_failures).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
