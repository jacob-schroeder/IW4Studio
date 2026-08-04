namespace IW4.Studio.Rendering;

public enum MenuLocalizationStatus
{
    Literal = 0,
    Resolved = 1,
    Missing = 2
}

/// <summary>
/// Immutable localization decision. AuthoredText is never normalized or
/// replaced, so an editor can display and serialize the original @KEY while
/// rendering DisplayText from target authoring or the active canonical
/// Localize provider.
/// </summary>
public sealed class MenuLocalizedTextResolution
{
    private MenuLocalizedTextResolution(
        MenuLocalizationStatus status,
        string authoredText,
        string displayText,
        string? lookupName,
        string? resolvedAssetName,
        string? failure,
        MenuTextResourceRevision resourceRevision)
    {
        Status = status;
        AuthoredText = authoredText;
        DisplayText = displayText;
        LookupName = lookupName;
        ResolvedAssetName = resolvedAssetName;
        Failure = failure;
        ResourceRevision = resourceRevision;
    }

    public MenuLocalizationStatus Status { get; }

    public string AuthoredText { get; }

    public string DisplayText { get; }

    /// <summary>The requested identity without the authored leading '@'.</summary>
    public string? LookupName { get; }

    public string? ResolvedAssetName { get; }

    public string? Failure { get; }

    public MenuTextResourceRevision ResourceRevision { get; }

    public bool IsResolved => Status == MenuLocalizationStatus.Resolved;

    internal static MenuLocalizedTextResolution Literal(
        string authoredText,
        MenuTextResourceRevision resourceRevision) =>
        new(
            MenuLocalizationStatus.Literal,
            authoredText,
            authoredText,
            null,
            null,
            null,
            resourceRevision);

    internal static MenuLocalizedTextResolution Resolved(
        string authoredText,
        string lookupName,
        string resolvedAssetName,
        string value,
        MenuTextResourceRevision resourceRevision) =>
        new(
            MenuLocalizationStatus.Resolved,
            authoredText,
            value,
            lookupName,
            resolvedAssetName,
            null,
            resourceRevision);

    internal static MenuLocalizedTextResolution Missing(
        string authoredText,
        string lookupName,
        string failure,
        MenuTextResourceRevision resourceRevision) =>
        new(
            MenuLocalizationStatus.Missing,
            authoredText,
            authoredText,
            lookupName,
            null,
            failure,
            resourceRevision);
}
