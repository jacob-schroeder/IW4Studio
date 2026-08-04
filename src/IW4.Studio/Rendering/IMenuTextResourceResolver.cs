namespace IW4.Studio.Rendering;

/// <summary>
/// Presentation-neutral access to the canonical localization and font assets
/// used by a Menu preview session.
/// </summary>
public interface IMenuTextResourceResolver
{
    /// <summary>
    /// Changes whenever resolving the same localization or font identity may
    /// produce a different canonical provider.
    /// </summary>
    long Revision { get; }

    MenuLocalizedTextResolution ResolveText(string authoredText);

    MenuFontAssetResolution ResolveFont(
        int fontEnum,
        MenuFontSelectionContext? context = null);
}
