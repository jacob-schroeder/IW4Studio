namespace IW4.Studio.Rendering;

/// <summary>
/// Exact source versions used by one Menu text-resource decision. Fonts are
/// supplied by the runtime pool, while localization may be supplied by the
/// live target authoring document before falling back to that pool.
/// </summary>
public readonly record struct MenuTextResourceRevision
{
    public MenuTextResourceRevision(
        long assetPoolRevision,
        long editingRevision)
    {
        if (assetPoolRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(assetPoolRevision));
        if (editingRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(editingRevision));

        AssetPoolRevision = assetPoolRevision;
        EditingRevision = editingRevision;
    }

    public long AssetPoolRevision { get; }

    public long EditingRevision { get; }
}

/// <summary>
/// Presentation-neutral access to authored localization and canonical font
/// resources used by a Menu preview session.
/// </summary>
public interface IMenuTextResourceResolver
{
    /// <summary>
    /// Changes whenever resolving the same localization or font identity may
    /// produce a different result.
    /// </summary>
    MenuTextResourceRevision Revision { get; }

    /// <summary>
    /// Raised when a live authoring change can alter localization results.
    /// Consumers should discard rendered text and reevaluate simulated Menu
    /// expressions without changing the authored Menu document.
    /// </summary>
    event EventHandler? Changed;

    MenuLocalizedTextResolution ResolveText(string authoredText);

    MenuFontAssetResolution ResolveFont(
        int fontEnum,
        MenuFontSelectionContext? context = null);
}
