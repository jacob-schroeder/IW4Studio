namespace IW4.Studio.Desktop.Rendering;

/// <summary>
/// Compact presentation state emitted after a material-preview attempt.
/// Decoded image bytes and workspace provider objects are deliberately not
/// retained by the Menu editor view model.
/// </summary>
public sealed record MenuPreviewMaterialStatus(
    string MaterialName,
    bool IsResolved,
    int FidelityIssueCount,
    string Detail);
