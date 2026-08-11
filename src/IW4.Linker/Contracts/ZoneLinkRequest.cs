namespace IW4.Linker.Contracts;

/// <summary>
/// Immutable canonical-link input. Provider precedence and root occurrence
/// order are both preserved exactly as supplied.
/// </summary>
public sealed class ZoneLinkRequest
{
    private readonly IReadOnlyList<LinkRoot> _roots;

    public ZoneLinkRequest(
        LinkAssetPool assets,
        IEnumerable<LinkRoot> roots)
    {
        Assets = assets ?? throw new ArgumentNullException(nameof(assets));
        ArgumentNullException.ThrowIfNull(roots);

        LinkRoot[] copied = roots
            .Select(root => root ?? throw new ArgumentException(
                "Link roots cannot contain null.",
                nameof(roots)))
            .ToArray();
        if (copied.Select(root => root.EntryId)
            .Distinct(StringComparer.Ordinal)
            .Count() != copied.Length)
        {
            throw new ArgumentException(
                "Link root entry IDs must be unique.",
                nameof(roots));
        }

        _roots = Array.AsReadOnly(copied);
    }

    public LinkAssetPool Assets { get; }
    public IReadOnlyList<LinkRoot> Roots => _roots;
}
