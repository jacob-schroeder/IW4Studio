using System.Numerics;
using IW4.FastFiles.Database;

namespace IW4.Linker.Contracts;

/// <summary>
/// Immutable canonical-link input. Provider precedence and root occurrence
/// order are both preserved exactly as supplied. Provider-bearing roots must
/// be unique. Native provider-pointer dependencies must be publishable before
/// use so each canonical XAsset row can retain an inline definition; name-only
/// closure edges do not constrain root order. <see cref="Linking.ZoneLinker"/>
/// validates those graph invariants before emission.
/// </summary>
public sealed class ZoneLinkRequest
{
    private readonly IReadOnlyList<LinkRoot> _roots;

    public ZoneLinkRequest(
        LinkAssetPool assets,
        IEnumerable<LinkRoot> roots,
        uint languageMask,
        uint selectedLanguageMask)
    {
        Assets = assets ?? throw new ArgumentNullException(nameof(assets));
        ArgumentNullException.ThrowIfNull(roots);
        if (!DbLanguageMask.IsSupported(languageMask))
        {
            throw new ArgumentOutOfRangeException(
                nameof(languageMask),
                "Language mask must contain supported PS3 IW4 language bits.");
        }
        if (!DbLanguageMask.IsSingleLanguage(selectedLanguageMask) ||
            (selectedLanguageMask & languageMask) == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selectedLanguageMask),
                "Selected language must be one bit present in the language mask.");
        }

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
        LanguageMask = languageMask;
        SelectedLanguageMask = selectedLanguageMask;
    }

    public LinkAssetPool Assets { get; }
    public IReadOnlyList<LinkRoot> Roots => _roots;
    public uint LanguageMask { get; }
    public uint SelectedLanguageMask { get; }

    internal int LanguageCount => BitOperations.PopCount(LanguageMask);
}
