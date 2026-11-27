using System.Numerics;
using IW4.Game.Database;

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
    private readonly IReadOnlyList<string?> _scriptStrings;
    private readonly Lazy<LinkAssetPool> _assets;

    public ZoneLinkRequest(
        LinkAssetPool assets,
        IEnumerable<LinkRoot> roots,
        uint languageMask,
        uint selectedLanguageMask,
        IEnumerable<string?> scriptStrings)
        : this(new Lazy<LinkAssetPool>(assets ?? throw new ArgumentNullException(nameof(assets))),
            roots, languageMask, selectedLanguageMask, scriptStrings)
    {
    }

    /// <summary>Prepares providers on first use from inputs owned by this revision.</summary>
    public ZoneLinkRequest(
        Lazy<LinkAssetPool> assets,
        IEnumerable<LinkRoot> roots,
        uint languageMask,
        uint selectedLanguageMask,
        IEnumerable<string?> scriptStrings)
    {
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(scriptStrings);
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
        string?[] copiedScriptStrings = scriptStrings.ToArray();
        if (copiedScriptStrings.Length > ushort.MaxValue + 1)
        {
            throw new ArgumentException(
                "Script-string tables cannot exceed the 16-bit zone-local index range.",
                nameof(scriptStrings));
        }
        _scriptStrings = Array.AsReadOnly(copiedScriptStrings);
        LanguageMask = languageMask;
        SelectedLanguageMask = selectedLanguageMask;
    }

    public LinkAssetPool Assets => _assets.Value;
    public IReadOnlyList<LinkRoot> Roots => _roots;
    /// <summary>Explicit standalone strings to emit in addition to values used by assets.</summary>
    public IReadOnlyList<string?> ScriptStrings => _scriptStrings;
    public uint LanguageMask { get; }
    public uint SelectedLanguageMask { get; }

    public ZoneLinkRequest WithMetadata(uint languageMask, uint selectedLanguageMask, IEnumerable<string?> scriptStrings)
        => new(_assets, Roots, languageMask, selectedLanguageMask, scriptStrings);

    internal int LanguageCount => BitOperations.PopCount(LanguageMask);
}
